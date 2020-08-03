//-----------------------------------------------------------------------------
// Filename: RawPacket.cs
//
// TODO: This class should be replaced by the existing RTP packet implementation
// in src/net/RTP.
//
// Description: See below.
//
// Derived From:
// https://github.com/RestComm/media-core/blob/master/rtp/src/main/java/org/restcomm/media/core/rtp/crypto/RawPacket.java
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 01 Jul 2020	Rafael Soares   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// Original Source: AGPL-3.0 License
//-----------------------------------------------------------------------------

/**
* When using TransformConnector, a RTP/RTCP packet is represented using
* RawPacket. RawPacket stores the buffer holding the RTP/RTCP packet, as well
* as the inner offset and length of RTP/RTCP packet data.
*
* After transformation, data is also store in RawPacket objects, either the
* original RawPacket (in place transformation), or a newly created RawPacket.
*
* Besides packet info storage, RawPacket also provides some other operations
* such as readInt() to ease the development process.
*
* @author Werner Dittmann (Werner.Dittmann@t-online.de)
* @author Bing SU (nova.su@gmail.com)
* @author Emil Ivov
* @author Damian Minkov
* @author Boris Grozev
* @author Lyubomir Marinov
* 
*/

using System.IO;

namespace SIPSorcery.Net
{
    public class RawPacket
    {

        public const int RTP_PACKET_MAX_SIZE = 8192;
        /**
         * The size of the extension header as defined by RFC 3550.
         */
        public const int EXT_HEADER_SIZE = 4;

        /**
         * The size of the fixed part of the RTP header as defined by RFC 3550.
         */
        public const int FIXED_HEADER_SIZE = 12;

        /**
         * Byte array storing the content of this Packet
         */
        private MemoryStream buffer;

        /**
         * Initializes a new empty <tt>RawPacket</tt> instance.
         */
        public RawPacket()
        {
            this.buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
        }

        /**
         * Initializes a new <tt>RawPacket</tt> instance with a specific
         * <tt>byte</tt> array buffer.
         *
         * @param buffer the <tt>byte</tt> array to be the buffer of the new
         * instance 
         * @param offset the offset in <tt>buffer</tt> at which the actual data to
         * be represented by the new instance starts
         * @param length the number of <tt>byte</tt>s in <tt>buffer</tt> which
         * constitute the actual data to be represented by the new instance
         */
        public RawPacket(byte[] data, int offset, int length)
        {
            this.buffer = new MemoryStream(RTP_PACKET_MAX_SIZE);
            Wrap(data, offset, length);
        }

        public void Wrap(byte[] data, int offset, int length)
        {
            this.buffer.Position = 0;
            this.buffer.Write(data, offset, length);
            this.buffer.SetLength(length - offset);
            this.buffer.Position = 0;
        }

        public byte[] GetData()
        {
            this.buffer.Position = 0;
            byte[] data = new byte[this.buffer.Length];
            this.buffer.Read(data, 0, data.Length);
            return data;
        }

        /**
         * Append a byte array to the end of the packet. This may change the data
         * buffer of this packet.
         *
         * @param data byte array to append
         * @param len the number of bytes to append
         */
        public void Append(byte[] data, int len)
        {
            if (data == null || len <= 0 || len > data.Length)
            {
                throw new System.Exception("Invalid combination of parameters data and length to append()");
            }

            long oldLimit = buffer.Length;
            // grow buffer if necessary
            Grow(len);
            // set positing to begin writing immediately after the last byte of the current buffer
            buffer.Position = oldLimit;
            // set the buffer limit to exactly the old size plus the new appendix length
            buffer.SetLength(oldLimit + len);
            // append data
            buffer.Write(data, 0, len);
        }

        /**
         * Get buffer containing the content of this packet
         *
         * @return buffer containing the content of this packet
         */
        public MemoryStream GetBuffer()
        {
            return this.buffer;
        }

        /**
         * Returns <tt>true</tt> if the extension bit of this packet has been set
         * and <tt>false</tt> otherwise.
         *
         * @return  <tt>true</tt> if the extension bit of this packet has been set
         * and <tt>false</tt> otherwise.
         */
        public bool GetExtensionBit()
        {
            return (this.buffer.GetBuffer()[0] & 0x10) == 0x10;
        }

        /**
         * Returns the length of the extensions currently added to this packet.
         *
         * @return the length of the extensions currently added to this packet.
         */
        public int GetExtensionLength()
        {
            int length = 0;
            if (GetExtensionBit())
            {
                // the extension length comes after the RTP header, the CSRC list,
                // and after two bytes in the extension header called "defined by profile"
                int extLenIndex = FIXED_HEADER_SIZE + GetCsrcCount() * 4 + 2;
                buffer.Position = extLenIndex;
                int byteLength = (buffer.ReadByte() << 8);
                int byteLength2 = buffer.ReadByte();

                length = (byteLength | byteLength2 * 4);
            }
            return length;
        }

        /**
         * Returns the number of CSRC identifiers currently included in this packet.
         *
         * @return the CSRC count for this <tt>RawPacket</tt>.
         */
        public int GetCsrcCount()
        {
            return (this.buffer.GetBuffer()[0] & 0x0f);
        }

        /**
         * Get RTP header length from a RTP packet
         *
         * @return RTP header length from source RTP packet
         */
        public int GetHeaderLength()
        {
            int length = FIXED_HEADER_SIZE + 4 * GetCsrcCount();
            if (GetExtensionBit())
            {
                length += EXT_HEADER_SIZE + GetExtensionLength();
            }
            return length;
        }

        /**
         * Get the length of this packet's data
         *
         * @return length of this packet's data
         */
        public int GetLength()
        {
            return (int)this.buffer.Length;
        }

        /**
         * Get RTP padding size from a RTP packet
         *
         * @return RTP padding size from source RTP packet
         */
        public int GetPaddingSize()
        {
            buffer.Position = 0;
            if ((this.buffer.ReadByte() & 0x20) == 0)
            {
                return 0;
            }
            buffer.Position = this.buffer.Length - 1;
            return this.buffer.ReadByte();
        }

        /**
         * Get the RTP payload (bytes) of this RTP packet.
         *
         * @return an array of <tt>byte</tt>s which represents the RTP payload of
         * this RTP packet
         */
        public byte[] GetPayload()
        {
            return ReadRegion(GetHeaderLength(), GetPayloadLength());
        }

        /**
         * Get RTP payload length from a RTP packet
         *
         * @return RTP payload length from source RTP packet
         */
        public int GetPayloadLength()
        {
            return GetLength() - GetHeaderLength();
        }

        /**
         * Get RTP payload type from a RTP packet
         *
         * @return RTP payload type of source RTP packet
         */
        public byte GetPayloadType()
        {
            buffer.Position = 1;
            return (byte)(this.buffer.ReadByte() & (byte)0x7F);
        }

        /**
         * Get RTCP SSRC from a RTCP packet
         *
         * @return RTP SSRC from source RTP packet
         */
        public int GetRTCPSSRC()
        {
            return ReadInt(4);
        }

        /**
         * Get RTP sequence number from a RTP packet
         *
         * @return RTP sequence num from source packet
         */
        public int GetSequenceNumber()
        {
            return ReadUnsignedShortAsInt(2);
        }

        /**
         * Get SRTCP sequence number from a SRTCP packet
         *
         * @param authTagLen authentication tag length
         * @return SRTCP sequence num from source packet
         */
        public int GetSRTCPIndex(int authTagLen)
        {
            int offset = GetLength() - (4 + authTagLen);
            return ReadInt(offset);
        }

        /**
         * Get RTP SSRC from a RTP packet
         *
         * @return RTP SSRC from source RTP packet
         */
        public int GetSSRC()
        {
            return ReadInt(8);
        }

        /**
         * Returns the timestamp for this RTP <tt>RawPacket</tt>.
         *
         * @return the timestamp for this RTP <tt>RawPacket</tt>.
         */
        public long GetTimestamp()
        {
            return ReadInt(4);
        }

        /**
         * Grow the internal packet buffer.
         *
         * This will change the data buffer of this packet but not the
         * length of the valid data. Use this to grow the internal buffer
         * to avoid buffer re-allocations when appending data.
         *
         * @param delta number of bytes to grow
         */
        public void Grow(int delta)
        {
            if (delta == 0)
            {
                return;
            }

            long newLen = buffer.Length + delta;
            if (newLen <= buffer.Capacity)
            {
                // there is more room in the underlying reserved buffer memory
                buffer.SetLength(newLen);
                return;
            }
            else
            {
                // create a new bigger buffer
                MemoryStream newBuffer = new MemoryStream();
                buffer.Position = 0;
                newBuffer.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
                newBuffer.SetLength(newLen);
                // switch to new buffer
                buffer = newBuffer;
            }
        }

        /**
         * Read a integer from this packet at specified offset
         *
         * @param off start offset of the integer to be read
         * @return the integer to be read
         */
        public int ReadInt(int off)
        {
            buffer.Position = off;
            return ((buffer.ReadByte() & 0xff) << 24) |
                    ((buffer.ReadByte() & 0xff) << 16) |
                    ((buffer.ReadByte() & 0xff) << 8) |
                    ((buffer.ReadByte() & 0xff));
        }

        /**
         * Read a byte region from specified offset with specified length
         *
         * @param off start offset of the region to be read
         * @param len length of the region to be read
         * @return byte array of [offset, offset + length)
         */
        public byte[] ReadRegion(int off, int len)
        {
            this.buffer.Position = 0;
            if (off < 0 || len <= 0 || off + len > this.buffer.Length)
            {
                return null;
            }

            byte[] region = new byte[len];
            this.buffer.Read(region, off, len);
            return region;
        }

        /**
         * Read a byte region from specified offset in the RTP packet and with
         * specified length into a given buffer
         * 
         * @param off
         *            start offset in the RTP packet of the region to be read
         * @param len
         *            length of the region to be read
         * @param outBuff
         *            output buffer
         */
        public void ReadRegionToBuff(int off, int len, byte[] outBuff)
        {
            buffer.Position = off;
            buffer.Read(outBuff, 0, len);
        }

        /**
         * Read an unsigned short at specified offset as a int
         *
         * @param off start offset of the unsigned short
         * @return the int value of the unsigned short at offset
         */
        public int ReadUnsignedShortAsInt(int off)
        {
            this.buffer.Position = off;
            int b1 = (0x000000FF & (this.buffer.ReadByte()));
            int b2 = (0x000000FF & (this.buffer.ReadByte()));
            int val = b1 << 8 | b2;
            return val;
        }

        /**
         * Read an unsigned integer as long at specified offset
         *
         * @param off start offset of this unsigned integer
         * @return unsigned integer as long at offset
         */
        public long ReadUnsignedIntAsLong(int off)
        {
            buffer.Position = off;
            return (((long)(buffer.ReadByte() & 0xff) << 24) |
                    ((long)(buffer.ReadByte() & 0xff) << 16) |
                    ((long)(buffer.ReadByte() & 0xff) << 8) |
                    ((long)(buffer.ReadByte() & 0xff))) & 0xFFFFFFFFL;
        }

        /**
         * Shrink the buffer of this packet by specified length
         *
         * @param len length to shrink
         */
        public void shrink(int delta)
        {
            if (delta <= 0)
            {
                return;
            }

            long newLimit = buffer.Length - delta;
            if (newLimit <= 0)
            {
                newLimit = 0;
            }
            this.buffer.SetLength(newLimit);
        }
    }
}
