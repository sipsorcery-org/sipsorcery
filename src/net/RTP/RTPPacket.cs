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

namespace SIPSorcery.Net
{
    public class RTPPacket
    {
        public RTPHeader Header;
        public byte[] Payload;

        public RTPPacket()
        {
            Header = new RTPHeader();
        }

        public RTPPacket(int payloadSize)
        {
            Header = new RTPHeader();
            Payload = new byte[payloadSize];
        }

        public RTPPacket(byte[] packet)
        {
            Header = new RTPHeader(packet);
            Payload = new byte[Header.PayloadSize];
            Array.Copy(packet, Header.Length, Payload, 0, Payload.Length);
        }

        public byte[] GetBytes()
        {
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + Payload.Length];

            Array.Copy(header, packet, header.Length);
            Array.Copy(Payload, 0, packet, header.Length, Payload.Length);

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
                packet.Payload = buffer.Slice(headerConsumed, header.PayloadSize).ToArray();
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