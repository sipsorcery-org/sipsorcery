//-----------------------------------------------------------------------------
// Filename: RTCPHeader.cs
//
// Description: RTCP Header as defined in RFC3550.
// 
//
//      RTCP Header
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Payload                               |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// (V)ersion (2 bits) = 2
// (P)adding (1 bit) = Inidcates whether the packet contains additional padding octets.
// Reception Report Count (RC) (5 bits) = The number of reception report blocks contained in this packet. A
//      value of zero is valid.
// Packet Type (PT) (8 bits) = Contains the constant 200 to identify this as an RTCP SR packet.
// Length (16 bits) = The length of this RTCP packet in 32-bit words minus one, including the header and any padding.
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
	public class RTCPHeader
	{
		public const int HEADER_BYTES_LENGTH = 4;
        public const int MAX_RECEPTIONREPORT_COUNT = 32;

		public const int RTCP_VERSION = 2;
        public const UInt16 RTCP_PACKET_TYPE = 200;             // 200 for Sender Report.

		public int Version = RTCP_VERSION;						// 2 bits.
		public int PaddingFlag = 0;								// 1 bit.
        public int ReceptionReportCount = 0;                    // 5 bits.
        public UInt16 PacketType = RTCP_PACKET_TYPE;            // 8 bits.
        public UInt16 Length;                                   // 16 bits.

		public RTCPHeader()
		{}

		/// <summary>
		/// Extract and load the RTP header from an RTP packet.
		/// </summary>
		/// <param name="packet"></param>
		public RTCPHeader(byte[] packet)
		{
            if (packet.Length < HEADER_BYTES_LENGTH)
			{
				throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP header packet.");
			}

            UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

            if (BitConverter.IsLittleEndian)
            {
                firstWord = NetConvert.DoReverseEndian(firstWord);
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                Length = BitConverter.ToUInt16(packet, 2);
            }

			Version = Convert.ToInt32(firstWord >> 14);
			PaddingFlag = Convert.ToInt32((firstWord >> 13) & 0x1);
			ReceptionReportCount = Convert.ToInt32((firstWord >> 8) & 0x1f);
            PacketType = Convert.ToUInt16(firstWord & 0x00ff);
		}

		public byte[] GetHeader(int receptionReportCount, UInt16 length)
		{
            if (receptionReportCount > MAX_RECEPTIONREPORT_COUNT)
            {
                throw new ApplicationException("The Reception Report Count value cannot be larger than " + MAX_RECEPTIONREPORT_COUNT + ".");
            }

            ReceptionReportCount = receptionReportCount;
            Length = length;

			return GetBytes();
		}

		public byte[] GetBytes()
		{
			byte[] header = new byte[4];

            UInt32 firstWord = Convert.ToUInt32(Version * Math.Pow(2, 30) + PaddingFlag * Math.Pow(2, 29) + ReceptionReportCount * Math.Pow(2, 24) + PacketType * Math.Pow(2, 16) + Length);

			if(BitConverter.IsLittleEndian)
			{
				Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 4);
			}
			else
			{
				Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 4);
			}

			return header;
		}
	}
}
