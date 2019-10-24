//-----------------------------------------------------------------------------
// Filename: RTCPPacket.cs
//
// Description: Encapsulation of an RTCP (Real Time Control Protocol) packet.
//
//      RTCP Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         SSRC of sender                        |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//sender |              NTP timestamp, most significant word             |
//info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |             NTP timestamp, least significant word             |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         RTP timestamp                         |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                     sender's packet count                     |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                      sender's octet count                     |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//report |                 SSRC_1 (SSRC of first source)                 |
//block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  1    | fraction lost |       cumulative number of packets lost       |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |           extended highest sequence number received           |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                      interarrival jitter                      |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         last SR (LSR)                         |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                   delay since last SR (DLSR)                  |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//report |                 SSRC_2 (SSRC of second source)                |
//block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  2    :                               ...                             :
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//       |                  profile-specific extensions                  |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// 
// History:
// 22 Feb 2007	Aaron Clauson	Created.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007-2019 Aaron Clauson (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
	public class RTCPPacket
	{
        public const int SENDERINFO_BYTES_LENGTH = 24;
        
        public RTCPHeader Header;                               // 32 bits.
        public uint SenderSyncSource;							// 32 bits.
        public UInt64 NTPTimestamp;                              // 64 bits.
        public uint RTPTimestamp;                                // 32 bits.
        public uint SenderPacketCount;                           // 32 bits.
        public uint SenderOctetCount;                            // 32 bits.
        public byte[] Reports;

		public RTCPPacket(uint senderSyncSource, ulong ntpTimestamp, uint rtpTimestamp, uint senderPacketCount, uint senderOctetCount)
		{
			Header = new RTCPHeader();
            SenderSyncSource = senderSyncSource;
            NTPTimestamp = ntpTimestamp;
            RTPTimestamp = rtpTimestamp;
            SenderPacketCount = senderPacketCount;
            SenderOctetCount = senderOctetCount;
		}
		
		public RTCPPacket(byte[] packet)
		{
			Header = new RTCPHeader(packet);

            if (BitConverter.IsLittleEndian)
            {
                SenderSyncSource = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
                NTPTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 8));
                RTPTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 16));
                SenderPacketCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 20));
                SenderOctetCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 24));
            }
            else
            {
                SenderSyncSource = BitConverter.ToUInt32(packet, 4);
                NTPTimestamp = BitConverter.ToUInt64(packet, 8);
                RTPTimestamp = BitConverter.ToUInt32(packet, 16);
                SenderPacketCount = BitConverter.ToUInt32(packet, 20);
                SenderOctetCount = BitConverter.ToUInt32(packet, 24);
            }

            Reports = new byte[packet.Length - RTCPHeader.HEADER_BYTES_LENGTH - SENDERINFO_BYTES_LENGTH];
            Buffer.BlockCopy(packet, RTCPHeader.HEADER_BYTES_LENGTH + SENDERINFO_BYTES_LENGTH, Reports, 0, Reports.Length);
		}

        public byte[] GetBytes()
        {
            byte[] payload = new byte[SENDERINFO_BYTES_LENGTH];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderSyncSource)), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(NTPTimestamp)), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(RTPTimestamp)), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderPacketCount)), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderOctetCount)), 0, payload, 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SenderSyncSource), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NTPTimestamp), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(RTPTimestamp), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderPacketCount), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderOctetCount), 0, payload, 20, 4);
            }

            Header.Length = Convert.ToUInt16(payload.Length / 4);
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + payload.Length];
            Array.Copy(header, packet, header.Length);
            Array.Copy(payload, 0, packet, header.Length, payload.Length);

            return packet;
        }

		public byte[] GetBytes(byte[] reports)
		{
            Reports = reports;
            byte[] payload = new byte[SENDERINFO_BYTES_LENGTH + reports.Length];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderSyncSource)), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(NTPTimestamp)), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(RTPTimestamp)), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderPacketCount)), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderOctetCount)), 0, payload, 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SenderSyncSource), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NTPTimestamp), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(RTPTimestamp), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderPacketCount), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderOctetCount), 0, payload, 20, 4);
            }

            Buffer.BlockCopy(reports, 0, payload, 24, reports.Length);

            Header.Length = Convert.ToUInt16(payload.Length / 4);
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + payload.Length];
            Array.Copy(header, packet, header.Length);
            Array.Copy(payload, 0, packet, header.Length, payload.Length);

			return packet;
		}
	}
}
