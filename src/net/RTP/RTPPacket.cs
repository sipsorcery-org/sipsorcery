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
#if !NETCOREAPP2_1_OR_GREATER || NETFRAMEWORK
using System.Linq;
#endif

namespace SIPSorcery.Net
{
    public class RTPPacket
    {
        public RTPHeader Header;
        private byte[] _payload;
        private ArraySegment<byte> _payloadSegment;
        private int _srtpProtectionLength = 0;

        public byte[] Payload
        {
            get { return _payload; }
            set { _payload = value; }
        }

        public RTPPacket()
        {
            Header = new RTPHeader();
        }

        public RTPPacket(int payloadSize)
        {
            Header = new RTPHeader();
            _payload = new byte[payloadSize];
        }

        public RTPPacket(byte[] packet)
        {
            Header = new RTPHeader(packet);
            _payload = new byte[Header.PayloadSize];
            Array.Copy(packet, Header.Length, _payload, 0, _payload.Length);
        }

        public RTPPacket(ArraySegment<byte> packet, int srtpProtectionLength)
        {
            Header = new RTPHeader();
            _payloadSegment = packet;
            _srtpProtectionLength = srtpProtectionLength;
        }

        public uint GetPayloadLength()
        {
            return (uint)(_payload?.Length ?? _payloadSegment.Count);
        }

        public byte[] GetPayloadBytes()
        {
            Payload ??= _payloadSegment.ToArray();

            return Payload;
        }

        public byte GetPayloadByteAt(int index)
        {
#if NETCOREAPP2_1_OR_GREATER && !NETFRAMEWORK
            return _payload?[index] ?? _payloadSegment[index];
#else
            return _payload?[index] ?? _payloadSegment.ElementAt(index);
#endif
        }

        public ArraySegment<byte> GetPayloadSegment(int offset, int length)
        {
            if (_payload != null)
            {
                return new ArraySegment<byte>(_payload, offset, length);
            }

#if NETCOREAPP2_1_OR_GREATER && !NETFRAMEWORK
            return _payloadSegment.Slice(offset, length);
#else
            return new ArraySegment<byte>(_payloadSegment.Array!, offset + _payloadSegment.Offset, length);
#endif
        }

        public byte[] GetBytes()
        {
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + (_payload?.Length ?? _payloadSegment.Count) + _srtpProtectionLength];

            Array.Copy(header, packet, header.Length);

            if (_payloadSegment != null)
            {
#if NETCOREAPP2_1_OR_GREATER && !NETFRAMEWORK
                _payloadSegment.CopyTo(packet, header.Length);
#else
                Array.Copy(_payloadSegment.Array!, _payloadSegment.Offset, packet, header.Length, _payloadSegment.Count);
#endif
            }
            else if (_payload != null)
            {
                Array.Copy(_payload, 0, packet, header.Length, _payload.Length);
            }
            else
            {
                throw new ApplicationException("Either _payloadSegment or _payload should be defined");
            }

            return packet;
        }

        private byte[] GetNullPayload(int numBytes)
        {
            byte[] payload = new byte[numBytes];

            for (int byteCount = 0; byteCount < numBytes; byteCount++)
            {
                payload[byteCount] = 0xff;
            }

            return payload;
        }

        public static bool TryParse(
            ReadOnlySpan<byte> buffer,
            RTPPacket packet,
            out int consumed)
        {
            consumed = 0;
            if (RTPHeader.TryParse(buffer, out var header, out var headerConsumed))
            {
                packet.Header = header;
                consumed += headerConsumed;
                packet._payload = buffer.Slice(headerConsumed, header.PayloadSize).ToArray();
                consumed += header.PayloadSize;
                return true;
            }

            return false;
        }

        public static bool TryParse(
            ReadOnlySpan<byte> buffer,
            out RTPPacket packet,
            out int consumed)
        {
            packet = new RTPPacket();
            return TryParse(buffer, packet, out consumed);
        }
    }
}
