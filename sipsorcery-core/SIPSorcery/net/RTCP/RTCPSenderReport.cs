//-----------------------------------------------------------------------------
// Filename: RTCPSenderReport.cs
//
//        RTCP Snder Report Payload
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         SSRC of sender                        |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// sender |              NTP timestamp, most significant word             |
// info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |             NTP timestamp, least significant word             |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         RTP timestamp                         |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                     sender's packet count                     |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                      sender's octet count                     |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// 
// History:
// 12 Aug 2019  Aaron Clauson   Created.
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
	public class RTCPSenderReport
	{
        public const int PAYLOAD_SIZE = 24;

        public uint SSRC;
        public ulong NtpTimestamp;
        public uint RtpTimestamp;
        public uint PacketCount;
        public uint OctetCount;
        
        public RTCPSenderReport(uint ssrc, ulong ntpTimestamp, uint rtpTimestamp, uint packetCount, uint octetCount)
		{
            SSRC = ssrc;
            NtpTimestamp = ntpTimestamp;
            RtpTimestamp = rtpTimestamp;
            PacketCount = packetCount;
            OctetCount = octetCount;
		}

        public byte[] GetBytes()
        {
            byte[] payload = new byte[24];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(NtpTimestamp)), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(RtpTimestamp)), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PacketCount)), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(OctetCount)), 0, payload, 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NtpTimestamp), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(RtpTimestamp), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(PacketCount), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(OctetCount), 0, payload, 20, 4);
            }

            return payload;
        }
    }
}
