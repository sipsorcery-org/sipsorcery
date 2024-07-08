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
using System.IO;

namespace SIPSorcery.Net
{
    public class RTPPacket : IDisposable
    {
        // Maximum size of an RTP packet
        public const int RTP_PACKET_MAX_SIZE = 8192;

        // Size of the extension header as defined by RFC 3550
        public const int EXT_HEADER_SIZE = 4;

        // Size of the fixed part of the RTP header as defined by RFC 3550
        public const int FIXED_HEADER_SIZE = 12;

        // MemoryStream storing the content of this packet
        private MemoryStream _buffer;

        // The RTP header of this packet
        public RTPHeader Header { get; set; }

        // The payload of this RTP packet
        public byte[] Payload { get; private set; }

        /// <summary>
        /// Initializes a new empty RTPPacket instance.
        /// </summary>
        public RTPPacket()
        {
            _buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
            Header = new RTPHeader();
        }

        /// <summary>
        /// Initializes a new RTPPacket instance with a specific payload size.
        /// </summary>
        /// <param name="payloadSize">The size of the payload.</param>
        public RTPPacket(int payloadSize)
        {
            _buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
            Header = new RTPHeader();
            Payload = new byte[payloadSize];
        }

        /// <summary>
        /// Initializes a new RTPPacket instance with a specific byte array buffer.
        /// </summary>
        /// <param name="data">The byte array buffer.</param>
        /// <param name="offset">The offset in the buffer where data starts.</param>
        /// <param name="length">The length of the data.</param>
        public RTPPacket(byte[] data, int offset, int length)
        {
            _buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
            Wrap(data, offset, length);
        }

        /// <summary>
        /// Initializes a new RTPPacket instance from a byte array packet.
        /// </summary>
        /// <param name="packet">The byte array representing the packet.</param>
        public RTPPacket(byte[] packet)
        {
            _buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
            Header = new RTPHeader(packet);
            Payload = new byte[Header.PayloadSize];
            Array.Copy(packet, Header.Length, Payload, 0, Payload.Length);
            Wrap(packet, 0, packet.Length);
        }

        /// <summary>
        /// Wraps the specified byte array buffer into this RTPPacket.
        /// </summary>
        /// <param name="data">The byte array buffer to wrap.</param>
        /// <param name="offset">The offset in the data buffer where the actual data starts.</param>
        /// <param name="length">The length of the data to wrap.</param>
        public void Wrap(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            _buffer.Position = 0;
            _buffer.Write(data, offset, length);
            _buffer.SetLength(length);
            _buffer.Position = 0;
        }

        /// <summary>
        /// Wraps the specified ReadOnlySpan<byte> buffer into this RTPPacket.
        /// </summary>
        /// <param name="data">The ReadOnlySpan<byte> buffer to wrap.</param>
        public void Wrap(ReadOnlySpan<byte> data)
        {
            _buffer.Position = 0;
            _buffer.SetLength(0);
            data.CopyTo(new Span<byte>(_buffer.GetBuffer(), 0, data.Length));
            _buffer.SetLength(data.Length);
            _buffer.Position = 0;
        }

        /// <summary>
        /// Gets the data contained in this RTPPacket as a byte array.
        /// </summary>
        /// <returns>A byte array containing the data of this RTPPacket.</returns>
        public byte[] GetData()
        {
            _buffer.Position = 0;
            byte[] data = new byte[_buffer.Length];
            _buffer.Read(data, 0, data.Length);
            return data;
        }

        /// <summary>
        /// Gets the complete RTP packet as a byte array.
        /// </summary>
        /// <returns>A byte array representing the complete RTP packet.</returns>
        public byte[] GetBytes()
        {
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + Payload.Length];

            Array.Copy(header, packet, header.Length);
            Array.Copy(Payload, 0, packet, header.Length, Payload.Length);

            return packet;
        }

        /// <summary>
        /// Appends a byte array to the end of the packet.
        /// </summary>
        /// <param name="data">The byte array to append.</param>
        /// <param name="length">The number of bytes to append.</param>
        public void Append(byte[] data, int length)
        {
            if (data == null || length <= 0 || length > data.Length)
            {
                throw new ArgumentException("Invalid combination of parameters data and length to append()");
            }

            long oldLength = _buffer.Length;
            Grow(length);
            _buffer.Position = oldLength;
            _buffer.Write(data, 0, length);
            _buffer.SetLength(oldLength + length);
        }

        /// <summary>
        /// Gets the buffer containing the content of this packet.
        /// </summary>
        /// <returns>A MemoryStream containing the content of this packet.</returns>
        public MemoryStream GetBuffer() => _buffer;

        /// <summary>
        /// Checks if the extension bit of this packet has been set.
        /// </summary>
        /// <returns>True if the extension bit is set; otherwise, false.</returns>
        public bool GetExtensionBit()
        {
            _buffer.Position = 0;
            return (_buffer.ReadByte() & 0x10) == 0x10;
        }

        /// <summary>
        /// Gets the length of the extensions currently added to this packet.
        /// </summary>
        /// <returns>The length of the extensions in bytes.</returns>
        public int GetExtensionLength()
        {
            if (!GetExtensionBit())
            {
                return 0;
            }

            int extLenIndex = FIXED_HEADER_SIZE + GetCsrcCount() * 4 + 2;
            _buffer.Position = extLenIndex;
            int byteLength = (_buffer.ReadByte() << 8);
            byteLength |= _buffer.ReadByte();

            return byteLength * 4;
        }

        /// <summary>
        /// Gets the number of CSRC identifiers currently included in this packet.
        /// </summary>
        /// <returns>The CSRC count.</returns>
        public int GetCsrcCount()
        {
            _buffer.Position = 0;
            return _buffer.ReadByte() & 0x0f;
        }

        /// <summary>
        /// Gets the RTP header length from this packet.
        /// </summary>
        /// <returns>The RTP header length in bytes.</returns>
        public int GetHeaderLength()
        {
            int length = FIXED_HEADER_SIZE + 4 * GetCsrcCount();
            if (GetExtensionBit())
            {
                length += EXT_HEADER_SIZE + GetExtensionLength();
            }
            return length;
        }

        /// <summary>
        /// Gets the length of this packet's data.
        /// </summary>
        /// <returns>The length of the data in bytes.</returns>
        public int GetLength() => (int)_buffer.Length;

        /// <summary>
        /// Gets the RTP padding size from this packet.
        /// </summary>
        /// <returns>The RTP padding size in bytes.</returns>
        public int GetPaddingSize()
        {
            _buffer.Position = 0;
            if ((_buffer.ReadByte() & 0x20) == 0)
            {
                return 0;
            }
            _buffer.Position = _buffer.Length - 1;
            return _buffer.ReadByte();
        }

        /// <summary>
        /// Gets the RTP payload of this packet.
        /// </summary>
        /// <returns>A byte array containing the RTP payload.</returns>
        public byte[] GetPayload() => ReadRegion(GetHeaderLength(), GetPayloadLength());

        /// <summary>
        /// Gets the RTP payload length from this packet.
        /// </summary>
        /// <returns>The RTP payload length in bytes.</returns>
        public int GetPayloadLength() => GetLength() - GetHeaderLength();

        /// <summary>
        /// Gets the RTP payload type from this packet.
        /// </summary>
        /// <returns>The RTP payload type.</returns>
        public byte GetPayloadType()
        {
            _buffer.Position = 1;
            return (byte)(_buffer.ReadByte() & 0x7F);
        }

        /// <summary>
        /// Gets the RTCP SSRC from this packet.
        /// </summary>
        /// <returns>The RTCP SSRC.</returns>
        public int GetRTCPSSRC() => ReadInt(4);

        /// <summary>
        /// Gets the RTP sequence number from this packet.
        /// </summary>
        /// <returns>The RTP sequence number.</returns>
        public int GetSequenceNumber() => ReadUnsignedShortAsInt(2);

        /// <summary>
        /// Gets the SRTCP index from this packet.
        /// </summary>
        /// <param name="authTagLen">The length of the authentication tag.</param>
        /// <returns>The SRTCP index.</returns>
        public int GetSRTCPIndex(int authTagLen)
        {
            int offset = GetLength() - (4 + authTagLen);
            return ReadInt(offset);
        }

        /// <summary>
        /// Gets the RTP SSRC from this packet.
        /// </summary>
        /// <returns>The RTP SSRC.</returns>
        public int GetSSRC() => ReadInt(8);

        /// <summary>
        /// Gets the timestamp for this RTP packet.
        /// </summary>
        /// <returns>The timestamp.</returns>
        public long GetTimestamp() => ReadInt(4);

        /// <summary>
        /// Grows the internal packet buffer.
        /// </summary>
        /// <param name="delta">The number of bytes to grow.</param>
        public void Grow(int delta)
        {
            if (delta <= 0)
            {
                return;
            }

            long newLength = _buffer.Length + delta;
            if (newLength > _buffer.Capacity)
            {
                MemoryStream newBuffer = new MemoryStream((int)newLength);
                _buffer.Position = 0;
                _buffer.CopyTo(newBuffer);
                _buffer = newBuffer;
            }
            _buffer.SetLength(newLength);
        }

        /// <summary>
        /// Reads an integer from the packet at the specified offset.
        /// </summary>
        /// <param name="offset">The offset in the packet.</param>
        /// <returns>The integer value read from the packet.</returns>
        public int ReadInt(int offset)
        {
            _buffer.Position = offset;
            return (_buffer.ReadByte() << 24) |
                   (_buffer.ReadByte() << 16) |
                   (_buffer.ReadByte() << 8) |
                   _buffer.ReadByte();
        }

        /// <summary>
        /// Reads a byte region from the specified offset with the specified length.
        /// </summary>
        /// <param name="offset">The offset in the packet.</param>
        /// <param name="length">The length of the region to read.</param>
        /// <returns>A byte array containing the region read from the packet.</returns>
        public byte[] ReadRegion(int offset, int length)
        {
            if (offset < 0 || length <= 0 || offset + length > _buffer.Length)
            {
                return null;
            }

            byte[] region = new byte[length];
            _buffer.Position = offset;
            _buffer.Read(region, 0, length);
            return region;
        }

        /// <summary>
        /// Reads a region of bytes from the specified offset and length into the provided buffer.
        /// </summary>
        /// <param name="offset">The offset to start reading from.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="outBuffer">The buffer to store the read bytes.</param>
        public void ReadRegionToBuffer(int offset, int length, byte[] outBuffer)
        {
            if (outBuffer == null)
            {
                throw new ArgumentNullException(nameof(outBuffer));
            }
            if (offset < 0 || length <= 0 || offset + length > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }

            _buffer.Position = offset;
            _buffer.Read(outBuffer, 0, length);
        }

        /// <summary>
        /// Reads an unsigned short at the specified offset as an integer.
        /// </summary>
        /// <param name="offset">The offset in the packet.</param>
        /// <returns>The integer value of the unsigned short at the offset.</returns>
        public int ReadUnsignedShortAsInt(int offset)
        {
            _buffer.Position = offset;
            int b1 = _buffer.ReadByte();
            int b2 = _buffer.ReadByte();
            return (b1 << 8) | b2;
        }

        /// <summary>
        /// Reads an unsigned integer as a long at the specified offset.
        /// </summary>
        /// <param name="offset">The offset in the packet.</param>
        /// <returns>The long value of the unsigned integer at the offset.</returns>
        public long ReadUnsignedIntAsLong(int offset)
        {
            _buffer.Position = offset;
            return (uint)((_buffer.ReadByte() << 24) |
                          (_buffer.ReadByte() << 16) |
                          (_buffer.ReadByte() << 8) |
                          _buffer.ReadByte());
        }

        /// <summary>
        /// Shrinks the buffer of this packet by the specified length.
        /// </summary>
        /// <param name="delta">The number of bytes to shrink.</param>
        public void Shrink(int delta)
        {
            if (delta <= 0)
            {
                return;
            }

            long newLength = _buffer.Length - delta;
            if (newLength < 0)
            {
                newLength = 0;
            }
            _buffer.SetLength(newLength);
        }

        /// <summary>
        /// Attempts to parse the provided buffer into an RTPPacket.
        /// </summary>
        /// <param name="buffer">The buffer containing the packet data.</param>
        /// <param name="packet">The resulting RTPPacket if parsing is successful.</param>
        /// <param name="consumed">The number of bytes consumed from the buffer.</param>
        /// <returns>True if parsing is successful; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<byte> buffer, RTPPacket packet, out int consumed)
        {
            consumed = 0;
            if (RTPHeader.TryParse(buffer, out var header, out var headerConsumed))
            {
                packet.Header = header;
                consumed += headerConsumed;
                packet.Payload = buffer.Slice(headerConsumed, header.PayloadSize).ToArray();
                consumed += header.PayloadSize;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to parse the provided buffer into an RTPPacket.
        /// </summary>
        /// <param name="buffer">The buffer containing the packet data.</param>
        /// <param name="packet">The resulting RTPPacket if parsing is successful.</param>
        /// <param name="consumed">The number of bytes consumed from the buffer.</param>
        /// <returns>True if parsing is successful; otherwise, false.</returns>
        public static bool TryParse(ReadOnlySpan<byte> buffer, out RTPPacket packet, out int consumed)
        {
            packet = new RTPPacket();
            return TryParse(buffer, packet, out consumed);
        }

        /// <summary>
        /// Creates a null payload of the specified size.
        /// </summary>
        /// <param name="numBytes">The number of bytes for the null payload.</param>
        /// <returns>A byte array filled with 0xff values.</returns>
        private byte[] GetNullPayload(int numBytes)
        {
            byte[] payload = new byte[numBytes];

            for (int byteCount = 0; byteCount < numBytes; byteCount++)
            {
                payload[byteCount] = 0xff;
            }

            return payload;
        }

        /// <summary>
        /// Disposes the internal resources used by this RTPPacket.
        /// </summary>
        public void Dispose()
        {
            _buffer?.Dispose();
        }
    }

}
